#ifndef DOWNBEAT_MEADOW_WIND_INCLUDED
#define DOWNBEAT_MEADOW_WIND_INCLUDED

#include "MeadowBreeze.hlsl"

// The wind as the grass and the flowers feel it: the steady breeze (MeadowBreeze.hlsl), then the waves of the beat and the notes, then the hands. Waves come from WindField (Core/Meadow/WindField.cs) as a structured buffer of
// 48-byte structs: (originX, originZ, speed, kind) (amplitude, lifeSec, glow, age) (widthScale, 0, 0, 0). The strength of a wave at p is
//     amplitude * exp(-d^2 / (width * widthScale)^2) * max(0, 1 - age / life)
// with d = p.x - (originX + speed * age) for a sweep (kind 0) and |p - origin| - speed * age for a ring (kind 1): the same formula as WindField.SampleBend. A sweep with a negative speed runs towards -x and pushes that way.

struct MeadowWave
{
    float4 originSpeedKind;   // originX, originZ, speed, kind
    float4 ampLifeGlowAge;    // amplitude, lifeSec, glow, age
    float4 shape;             // widthScale (the front is this many times as wide as the field's wave width), then three zeros
};

StructuredBuffer<MeadowWave> _MeadowWaves;
float4 _MeadowWindA;   // x waveWidth, y bendScale, z the most a blade bends in total (radians; content meadow.wind.maxBendDeg)
float4 _MeadowWindB;   // x waveCount, y handRadius, z handPush, w handLean
float4 _MeadowHands[2];     // x, z (metres), active (0 or 1), unused
float4 _MeadowHandVel[2];   // vx, vz (metres per second)
float4 _MeadowHandGust;     // x width of the gust that goes with a hand (m), y the fastest hand that blows harder (m/s)

// xy: horizontal bend direction * strength (summed over the breeze, the waves and the hands), z: glow, w: strength of the waves alone (for brightening).
// h01: how far up the blade (0 foot, 1 tip), because a tip meets a gust a little later than the foot. gust: how strongly a gust is over the spot (brightens the tips).
float4 MeadowWindAt(float2 p, float bladePhase, float h01, float4 field, out float gust)
{
    float2 bend = 0;
    float glow = 0;
    float strength = 0;
    float width2 = _MeadowWindA.x * _MeadowWindA.x;
    int count = _MeadowWindMute.y < 0.5 ? 0 : (int)_MeadowWindB.x;
    for (int i = 0; i < count; i++)
    {
        MeadowWave w = _MeadowWaves[i];
        float age = w.ampLifeGlowAge.w;
        float travelled = w.originSpeedKind.z * age;
        float d;
        float2 dir;
        if (w.originSpeedKind.w < 0.5)
        {
            d = p.x - (w.originSpeedKind.x + travelled);
            dir = float2(w.originSpeedKind.z < 0.0 ? -1.0 : 1.0, 0);
        }
        else
        {
            float2 r = p - w.originSpeedKind.xy;
            float len = length(r);
            d = len - travelled;
            dir = r / max(len, 1e-3);
        }
        float profile = exp(-d * d / (width2 * w.shape.x * w.shape.x)) * max(0.0, 1.0 - age / w.ampLifeGlowAge.y);
        float s = w.ampLifeGlowAge.x * profile;
        bend += dir * s;
        strength += s;
        glow += w.ampLifeGlowAge.z * profile;
    }

    // the breeze: lean, gusts, sway (one direction, neighbours together; the blade's own number only sets how stiff it is and the phase of its tip)
    bend += MeadowBreezeBend(p, h01, bladePhase, field, gust);

    // hands: a soft gust that goes with each hand and fades into the breeze.
    //  - feather: (1 - x^2)^2 of the distance x to the hand over its radius: 1 at the hand, 0 with no slope at the radius, so there is no edge where the hand's wind ends and the breeze goes on
    //  - parting: the grass is pushed away from the hand. The push is nothing at the very spot of the hand (so the grass is clear there, no pinwheel) and most about 0.45 of the way out.
    //  - the gust: along the way the hand moves, with the same swing back behind it that the breeze's gusts have (MeadowGustProfile); a tip meets it a little later than the foot
    // Blades differ in how far they give, as in the breeze (MeadowStiffness).
    float handRadius = _MeadowWindB.y;
    for (int h = 0; h < 2; h++)
    {
        float4 hand = _MeadowHands[h];
        if (hand.z < 0.5 || _MeadowWindMute.z < 0.5) continue;
        float2 away = p - hand.xy;
        float x2 = dot(away, away) / (handRadius * handRadius);
        if (x2 >= 1.0) continue;
        float feather = (1.0 - x2) * (1.0 - x2);
        float stiffness = MeadowStiffness(bladePhase);
        bend += away / handRadius * (feather * _MeadowWindB.z * 3.5 * stiffness);   // 3.5 makes the peak of x(1-x^2)^2 (0.286, at x = 0.447) equal handPush

        float2 velocity = _MeadowHandVel[h].xy;
        float speed = length(velocity);
        float2 direction = velocity / max(speed, 1e-3);
        float cappedSpeed = min(speed, _MeadowHandGust.y);
        if (_MeadowSkyCheap > 0.5)
        {
            // a weak tier: the grass leans along the hand's way, feathered like the parting, and nothing more (no swing back: it costs a good part of the grass's vertex work)
            bend += direction * (feather * smoothstep(0.05, 0.4, speed) * cappedSpeed * _MeadowWindB.w);
        }
        else
        {
            float moving = smoothstep(0.05, 0.4, speed);   // a hand that hardly moves has no direction: its lean fades out instead of flipping
            float ahead = dot(away, direction) + _BreezeGustB.w * h01;
            float body = MeadowGustProfile(ahead, _MeadowHandGust.x, _BreezeGustC.x);
            bend += direction * (body * feather * moving * cappedSpeed * _MeadowWindB.w * stiffness);
        }
        strength += feather * 0.5;
    }

    return float4(bend, glow, strength);
}

// How a blade or a flower stem bends: along a circular arc as long as the blade is, never longer. The same numbers, line for line, as Core/Meadow/BladeBend.cs (BladeBendGpuParityPlayModeTests holds the two together).
//  - bendPerHeight: the push (MeadowWindAt's bend times bendScale, plus the blade's lean): its direction is the way the blade leans, its length s says how hard.
//  - the total bend theta grows like 2.4 x s (the resting meadow looks as it always did: the chord of the arc leans within 5 degrees of the old formula's tip up to s = 0.6) and saturates softly at
//    _MeadowWindA.z, the most a blade bends (120 degrees): theta = max x u / (1 + u^4)^(1/4), u = 2.4 s / max. Near a fast hand the blades lie down towards that angle instead of stretching into streaks,
//    a harder hand further; the knee is soft so that a blade that leans a lot on its own still bends when a gust comes.
// Returns the point at t (0 foot, 1 tip) of a blade of this height, relative to the foot (x, up, z), and the direction the blade goes there (a unit vector).
float3 MeadowBladeCurve(float2 bendPerHeight, float t, float height, out float3 tangent)
{
    float2 bend = clamp(bendPerHeight, -40.0, 40.0);
    float strength = length(bend);
    float maxBend = max(_MeadowWindA.z, 1e-3);
    float u = min(strength, 40.0) * 2.4 / maxBend;
    float u2 = u * u;
    float u4 = u2 * u2;
    float theta = maxBend * u / sqrt(sqrt(1.0 + u4));
    float2 direction = strength > 1e-20 ? bend / strength : float2(0.0, 0.0);

    float halfAngle = 0.5 * theta * t;
    float sinHalf, cosHalf;
    sincos(halfAngle, sinHalf, cosHalf);
    // (1 - cos a) / theta = t sin(a/2) sinc(a/2) and sin a / theta = t cos(a/2) sinc(a/2), with sinc(x) = sin x / x (the series 1 - x^2/6 where x is tiny)
    float sinc = halfAngle > 1e-3 ? sinHalf / halfAngle : 1.0 - halfAngle * halfAngle * (1.0 / 6.0);
    float sideways = height * t * sinHalf * sinc;
    float up = height * t * cosHalf * sinc;
    float lean = 2.0 * sinHalf * cosHalf;
    float cosA = cosHalf * cosHalf - sinHalf * sinHalf;
    tangent = float3(lean * direction.x, cosA, lean * direction.y);
    return float3(sideways * direction.x, up, sideways * direction.y);
}

#endif
