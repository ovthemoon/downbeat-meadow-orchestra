#ifndef DOWNBEAT_MEADOW_BREEZE_INCLUDED
#define DOWNBEAT_MEADOW_BREEZE_INCLUDED

// The steady wind of the meadow, as a pure function of place and time. This is, line for line, Core/Meadow/BreezeModel.cs and BreezeHash.cs (and CloudNoise in the same folder): the tests
// compare the two (BreezeGpuParityTests), so change both together. Time arrives only as small wrapped numbers (see BreezePhase), never as a big float clock.
//   - one prevailing direction the grass leans in, with gusts that roll along it, a slow sway around the lean and a small slow motion of the tips
//   - soft cloud-deck shadows that slide over the ground in the same direction
// Bend values are fractions of a blade's height before bendScale.

float4 _MeadowWindMute;     // --wind-mute (measurements): 1 = the part works, 0 = switched off: x breeze (all of it), y waves, z hands, w sparkle
float4 _MeadowWindMute2;    // x gusts, y sway, z flutter

float4 _BreezeDir;          // x, y: the way the wind blows on the ground (unit); z, w: across it (unit)
float4 _BreezeA;            // x lean, y mean variation, z stiffness min, w stiffness max
float4 _BreezeGustA;        // x spacing, y width, z amplitude, w weakest
float4 _BreezeGustB;        // x jitter, y front wobble, z wobble length, w lag (metres)
float4 _BreezeGustC;        // x spring-back, y brightness, z sway wavelength, w sway amplitude
float4 _BreezeFlutter;      // x amplitude
float4 _BreezePhaseA;       // x gust shift in its cell (m), y gust cell number, z sway phase, w flutter phase
float4 _BreezePhaseB;       // x mean phase
float4 _CloudShadowA;       // x, y: shift of the shadows (m, wrapped), z cell size (m), w strength
float4 _CloudShadowB;       // x coverage, y softness

// The gusts and the cloud shadows are worked out once per frame for a grid of ground points (BreezeField.cs, BreezeBake.shader) and read from there: hundreds of thousands of grass
// vertices and the pixels of the painted far meadow all want them, and each working out three gusts would cost more than the rest of the meadow. x, y, z: the gust as the foot, the middle
// and the tip of a blade meet it, w: the cloud shadow.
TEXTURE2D(_BreezeFieldTex);
SAMPLER(sampler_BreezeFieldTex);
float4 _BreezeFieldRect;    // x, y: ground x, z of the grid's corner (m), z, w: 1 / its size (1 / m)

static const float BREEZE_TWO_PI = 6.2831853;
static const int BREEZE_GUST_WRAP = 4096;

float MeadowUnitHash(uint k)
{
    k ^= k >> 16;
    k *= 0x7feb352du;
    k ^= k >> 15;
    k *= 0x846ca68bu;
    k ^= k >> 16;
    return (k >> 8) * (1.0 / 16777216.0);
}

// One gust along the wind: ahead is the distance in front of its middle (negative behind it).
float MeadowGustProfile(float ahead, float width, float springBack)
{
    float scale = ahead > 0.0 ? width * 0.7 : width * 1.15;
    float q = ahead / scale;
    float body = exp(-q * q);
    float lobe = (ahead + 1.9 * width) / (0.9 * width);
    return body - springBack * exp(-lobe * lobe);
}

// The sum of the gusts at ground point p, as the top of a blade at h01 up its length meets them.
float MeadowGust(float2 p, float h01)
{
    float2 windDir = _BreezeDir.xy;
    float2 across2 = _BreezeDir.zw;
    float along = dot(p, windDir);
    float across = dot(p, across2);
    float spacing = _BreezeGustA.x;
    float cellPosition = (along - _BreezePhaseA.x) / spacing;
    float cellFloor = floor(cellPosition);
    float local = (cellPosition - cellFloor) * spacing;
    float crossFrequency = BREEZE_TWO_PI / _BreezeGustB.z * 0.6;
    float sum = 0.0;
    [unroll]
    for (int k = -1; k <= 2; k++)
    {
        uint id = (uint)(((int)cellFloor + k - (int)_BreezePhaseA.y) & (BREEZE_GUST_WRAP - 1));
        float strength = lerp(_BreezeGustA.w, 1.0, MeadowUnitHash(id * 4u));
        float shiftInCell = (MeadowUnitHash(id * 4u + 1u) * 2.0 - 1.0) * _BreezeGustB.x;
        float wobblePhase = BREEZE_TWO_PI * MeadowUnitHash(id * 4u + 2u);
        float crossPhase = BREEZE_TWO_PI * MeadowUnitHash(id * 4u + 3u);
        float centre = (k + 0.5 + shiftInCell) * spacing;
        float ahead = local - centre + _BreezeGustB.w * h01 + _BreezeGustB.y * sin(across * BREEZE_TWO_PI / _BreezeGustB.z + wobblePhase);
        float crossStrength = 0.8 + 0.2 * sin(across * crossFrequency + crossPhase);
        sum += strength * crossStrength * MeadowGustProfile(ahead, _BreezeGustA.y, _BreezeGustC.x);
    }
    return sum;
}

float4 MeadowBreezeField(float2 p)
{
    float2 uv = (p - _BreezeFieldRect.xy) * _BreezeFieldRect.zw;
    return SAMPLE_TEXTURE2D_LOD(_BreezeFieldTex, sampler_BreezeFieldTex, uv, 0.0);
}

// The gust at h01 up a blade: a parabola through the values for the foot, the middle and the tip (exact at those three heights).
float MeadowGustAtHeight(float4 field, float h01)
{
    return field.x * (1.0 - h01) * (1.0 - 2.0 * h01) + field.y * 4.0 * h01 * (1.0 - h01) + field.z * h01 * (2.0 * h01 - 1.0);
}

float MeadowStiffness(float bladePhase)
{
    return lerp(_BreezeA.z, _BreezeA.w, frac(bladePhase * 7.31 + 0.5));
}

// The horizontal bend of a blade at ground point p, h01 up its length; field is MeadowBreezeField(p). gust: the gust strength there (for the brightness of the tips).
float2 MeadowBreezeBend(float2 p, float h01, float bladePhase, float4 field, out float gust)
{
    float2 windDir = _BreezeDir.xy;
    float2 across2 = _BreezeDir.zw;
    float along = dot(p, windDir);
    float mean = 1.0 + _BreezeA.y * sin(BREEZE_TWO_PI * _BreezePhaseB.x);
    gust = MeadowGustAtHeight(field, h01);
    float swayArgument = BREEZE_TWO_PI * (along / _BreezeGustC.z - _BreezePhaseA.z);
    float push = (_BreezeA.x + _BreezeGustA.z * gust) * mean + _BreezeGustC.w * sin(swayArgument) * _MeadowWindMute2.y;
    float side = _BreezeGustC.w * 0.5 * cos(swayArgument) * _MeadowWindMute2.y;
    float stiffness = MeadowStiffness(bladePhase);
    push *= stiffness;
    side *= stiffness;

    float h3 = h01 * h01 * h01;
    float flutterArgument = BREEZE_TWO_PI * (_BreezePhaseA.w + 0.3 * bladePhase) + along * 0.7;
    push += _BreezeFlutter.x * h3 * sin(flutterArgument) * _MeadowWindMute2.z;
    side += _BreezeFlutter.x * 0.6 * h3 * cos(flutterArgument) * _MeadowWindMute2.z;
    return (windDir * push + across2 * side) * _MeadowWindMute.x;
}

// ---- cloud shadows ------------------------------------------------------------------------------------------------

float MeadowCloudLattice(int ix, int iz, int period)
{
    uint wx = (uint)(ix & (period - 1));
    uint wz = (uint)(iz & (period - 1));
    return MeadowUnitHash(wx * 73856093u ^ wz * 19349663u);
}

float MeadowCloudValue(float2 p, int period)
{
    float2 cell = floor(p);
    float2 t = p - cell;
    t = t * t * (3.0 - 2.0 * t);
    int ix = (int)cell.x;
    int iz = (int)cell.y;
    float a = MeadowCloudLattice(ix, iz, period);
    float b = MeadowCloudLattice(ix + 1, iz, period);
    float c = MeadowCloudLattice(ix, iz + 1, period);
    float d = MeadowCloudLattice(ix + 1, iz + 1, period);
    return lerp(lerp(a, b, t.x), lerp(c, d, t.x), t.y);
}

// Two octaves in 0..1, at lattice coordinates p (repeats every 256 cells).
float MeadowCloudFbm(float2 p)
{
    return 0.7 * MeadowCloudValue(p, 256) + 0.3 * MeadowCloudValue(p * 2.0 + float2(17.3, 5.1), 512);
}

// 0 in the light, 1 in the middle of a cloud-deck shadow, at ground point p.
float MeadowCloudShadow(float2 p)
{
    float noise = MeadowCloudFbm((p - _CloudShadowA.xy) / _CloudShadowA.z);
    float edge = 1.0 - _CloudShadowB.x;
    return smoothstep(edge - _CloudShadowB.y, edge + _CloudShadowB.y, noise);
}

// The factor a lit colour at ground point p is multiplied by: the cloud shadows darken the meadow.
float MeadowCloudLight(float4 field)
{
    return 1.0 - _CloudShadowA.w * field.w;
}

#endif
