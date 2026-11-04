#ifndef DOWNBEAT_MEADOW_COMMON_INCLUDED
#define DOWNBEAT_MEADOW_COMMON_INCLUDED

// Shared by every shader that is drawn in front of the backdrop plates (ground, grass, flowers, particles):
//   - the plates and their cross-fade weights, so that far ground can fade into the very pixel of the plate behind it
//   - the haze band where the 3D ground hands over to the plate
//   - the sun and sky colours that follow the three-stage sky arc
// All of it is set as globals by BackdropView, once per frame.

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "MeadowBreeze.hlsl"

TEXTURE2D(_MeadowPlate0);   // blue hour
TEXTURE2D(_MeadowPlate1);   // mid (the playing dusk)
TEXTURE2D(_MeadowPlate2);   // sunset
SAMPLER(sampler_MeadowPlate0);

float4 _MeadowSkyWeights;   // x blue hour, y mid, z sunset
float4 _MeadowPlateCover;   // xy: how much of the plate the screen shows (cover crop), zw: unused
float4 _MeadowHaze;         // x fromRow, y toRow (rows from the top of the picture), z plateFrom, w curve
float4 _MeadowHaze2;        // x blur level of the plate used as haze, y sharpFrom
float4 _MeadowFogColor;     // rgb, linear
float4 _MeadowHillPulse;    // x strength (0 = off), y centre picture row (from the top), z half height of the band, w how much it follows the painted haze (0 = flat band): the far hills' haze lights up on a drum beat
float4 _MeadowAmbientColor; // rgb, linear
float4 _MeadowSunColor;     // rgb: light colour x intensity, linear (the same as the main light, for shaders that want it without a light loop)
float4 _MeadowCamPos;       // xyz camera position, w height of the ground plane the far plate is mapped onto
float4 _MeadowCamRight;     // xyz: right * tan(half horizontal field of view)
float4 _MeadowCamUp;        // xyz: up * tan(half vertical field of view)
float4 _MeadowCamForward;   // xyz: forward
float4 _MeadowPlateRipple;  // x gain of the gust ripple, y row from which it is at full strength, z row above which there is none

// ---- plates -------------------------------------------------------------------------------------------------------

float2 MeadowPlateUV(float2 screenUV)
{
    return (screenUV - 0.5) * _MeadowPlateCover.xy + 0.5;
}

// The direction of the view ray through a screen position (0..1, y up): from the camera basis that BackdropView hands over for every camera that draws.
float3 MeadowViewRay(float2 screenUV)
{
    float2 ndc = screenUV * 2.0 - 1.0;
    return normalize(_MeadowCamForward.xyz + _MeadowCamRight.xyz * ndc.x + _MeadowCamUp.xyz * ndc.y);
}

// Everything moves at every quality tier. _MeadowSkyCheap is 1 at a weak one (meadow.quality[].skyDetail below 0.5), which only draws it more cheaply: the clouds are read from a baked tile instead of being
// worked out per pixel (BackdropBlend.shader), the hand gust is the plain lean (MeadowWind.hlsl), and the tip rim of the grass is left out (Grass.shader). The gust ripple and the cloud shadows of the far meadow
// are read from the baked breeze field either way.
float _MeadowSkyCheap;

// The painted far meadow lives: below the horizon every picture row is a stretch of ground far away, so the same gusts and cloud shadows that move over the 3D grass go on over the
// plate, at their place on the ground, in perspective. It is a factor the plate colour is multiplied by (1 over the sky, the hills and everything under the 3D ground, which is never
// seen as plate), so the hand-over from the 3D ground (which fades into this very plate) shows no seam. Worked out once per pixel, only where the plate shows.
float MeadowPlateLife(float2 screenUV)
{
    float row = 1.0 - screenUV.y;
    if (row > _MeadowHaze.x || row < _MeadowPlateRipple.z - 0.04) return 1.0;
    float2 ndc = screenUV * 2.0 - 1.0;
    float3 dir = _MeadowCamForward.xyz + _MeadowCamRight.xyz * ndc.x + _MeadowCamUp.xyz * ndc.y;   // not normalised: only where it meets the ground matters
    if (dir.y > -0.01) return 1.0;
    float2 ground = _MeadowCamPos.xz + dir.xz * ((_MeadowCamPos.w - _MeadowCamPos.y) / dir.y);
    float2 gridUV = (ground - _BreezeFieldRect.xy) * _BreezeFieldRect.zw;
    float inside = smoothstep(0.0, 0.06, min(min(gridUV.x, 1.0 - gridUV.x), min(gridUV.y, 1.0 - gridUV.y)));   // beyond the grid (very far away) the plate does not ripple
    float4 field = SAMPLE_TEXTURE2D_LOD(_BreezeFieldTex, sampler_BreezeFieldTex, gridUV, 0.0);
    float meadow = smoothstep(_MeadowPlateRipple.z, _MeadowPlateRipple.y, row) * inside;   // 0 over the hills, 1 over the far meadow
    float shadow = field.w * smoothstep(_MeadowPlateRipple.z - 0.04, _MeadowPlateRipple.y, row) * inside;
    return (1.0 + _MeadowPlateRipple.x * field.y * meadow) * (1.0 - _CloudShadowA.w * shadow);
}

// The plate cross-faded by the sky arc, at the given mip level (0 = sharp).
half3 MeadowSamplePlateLod(float2 screenUV, float lod)
{
    float2 uv = MeadowPlateUV(screenUV);
    half3 c = 0;
    if (_MeadowSkyWeights.x > 0.0005) c += SAMPLE_TEXTURE2D_LOD(_MeadowPlate0, sampler_MeadowPlate0, uv, lod).rgb * _MeadowSkyWeights.x;
    if (_MeadowSkyWeights.y > 0.0005) c += SAMPLE_TEXTURE2D_LOD(_MeadowPlate1, sampler_MeadowPlate0, uv, lod).rgb * _MeadowSkyWeights.y;
    if (_MeadowSkyWeights.z > 0.0005) c += SAMPLE_TEXTURE2D_LOD(_MeadowPlate2, sampler_MeadowPlate0, uv, lod).rgb * _MeadowSkyWeights.z;
    return c;
}

half3 MeadowSamplePlate(float2 screenUV)
{
    return MeadowSamplePlateLod(screenUV, 0.0);
}

// The drum of the hills lights the haze over them (_MeadowHillPulse: x strength, y centre row, z half height, w how much it follows the plate). How strongly it lights a place with this plate colour at this
// picture row: a band of rows keeps it off the sky and the meadow, and (the more _MeadowHillPulse.w) it follows what is painted: strongest where the plate is already hazy (its colour is near the fog
// colour: the mist in the valleys, the far ridges), weak over the dark trees and the clear near slopes, so it moves with the hills and is not one flat band.
static const float HillHazeSharpness = 14.0;   // how sharply the weight falls with the distance of the plate colour from the fog colour
static const float HillPulseFloor = 0.12;      // what is left of it over what is not hazy at all

float MeadowHillPulseWeight(float3 plate, float row)
{
    if (_MeadowHillPulse.x <= 0.0) return 0.0;   // no drum now: nothing to work out for the whole screen
    float band = saturate(1.0 - abs(row - _MeadowHillPulse.y) / max(_MeadowHillPulse.z, 1e-3));
    float3 away = plate - _MeadowFogColor.rgb;
    float haze = exp(-dot(away, away) * HillHazeSharpness);
    return band * band * lerp(1.0, lerp(HillPulseFloor, 1.0, haze), _MeadowHillPulse.w);
}

// The screen position (0..1, y up) of a fragment, from the clip-space (x, y, w) that the vertex shader passed on (as float3: x, y, w).
// Matches the position the backdrop quad draws its picture at, whichever way the render target is flipped.
float2 MeadowScreenUV(float3 clipXYW)
{
    float2 ndc = clipXYW.xy / clipXYW.z;
    ndc.y *= _ProjectionParams.x;
    return ndc * 0.5 + 0.5;
}

// ---- haze ---------------------------------------------------------------------------------------------------------

// The haze is a band of picture rows: fully 3D from _MeadowHaze.x (row from the top, 0.55 say) downwards, fully the plate from _MeadowHaze.y (0.46) upwards. Measured on the
// screen, not in the world, so the hand-over is the same across the whole width: no terrain bump, no field edge and no distance ring can show.
float MeadowHazeAmount(float row)
{
    float f = saturate((_MeadowHaze.x - row) / max(_MeadowHaze.x - _MeadowHaze.y, 1e-3));
    return pow(f, _MeadowHaze.w);
}

// Fades a lit colour into the haze. The haze is the stop's fog colour at first, then the plate behind the fragment, blurred; towards the end of the band
// the plate is sharp again, so that where the fade is complete the 3D ground and the painted ground are the same pixel and there is no seam.
// life: MeadowPlateLife(screenUV), worked out by the caller (the grass does it per vertex: its blades are thin and it has the most fragments).
half3 MeadowApplyHazeLife(half3 color, float2 screenUV, float life)
{
    float f = MeadowHazeAmount(1.0 - screenUV.y);
    if (f <= 0.0) return color;
    float plateAmount = smoothstep(0.0, max(_MeadowHaze.z, 1e-3), f);
    float sharpAmount = smoothstep(_MeadowHaze2.y, 1.0, f);
    half3 fog = _MeadowFogColor.rgb;
    if (plateAmount > 0.0)
    {
        half3 soft = MeadowSamplePlateLod(screenUV, _MeadowHaze2.x);
        half3 plate = sharpAmount > 0.0 ? lerp(soft, MeadowSamplePlateLod(screenUV, 0.0), sharpAmount) : soft;
        plate *= life;   // gusts and cloud shadows on the painted far meadow
        fog = lerp(fog, plate, plateAmount);
    }
    return lerp(color, fog, f);
}

half3 MeadowApplyHaze(half3 color, float2 screenUV)
{
    if (MeadowHazeAmount(1.0 - screenUV.y) <= 0.0) return color;
    return MeadowApplyHazeLife(color, screenUV, MeadowPlateLife(screenUV));
}

// ---- noise --------------------------------------------------------------------------------------------------------

float MeadowHash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float MeadowValueNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = MeadowHash21(i);
    float b = MeadowHash21(i + float2(1, 0));
    float c = MeadowHash21(i + float2(0, 1));
    float d = MeadowHash21(i + float2(1, 1));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

float MeadowFbm(float2 p)
{
    float v = 0.0;
    float a = 0.5;
    for (int i = 0; i < 4; i++)
    {
        v += a * MeadowValueNoise(p);
        p = p * 2.03 + 17.1;
        a *= 0.5;
    }
    return v;
}

#endif
