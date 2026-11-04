using Downbeat.Core;
using UnityEngine;

namespace Downbeat.Meadow
{
    /// <summary>
    /// Hands the wind to the shaders: the live waves of a <see cref="WindField"/> in a structured buffer, the breeze and hand settings, and where the hands are.
    /// Everything is a global (see MeadowWind.hlsl), so the grass, and later the flowers and particles, all feel the same wind.
    /// </summary>
    public sealed class WindFieldGpu : System.IDisposable
    {
        public const int Capacity = 16;
        const int HandSlots = 2;

        static readonly int WavesId = Shader.PropertyToID("_MeadowWaves");
        static readonly int WindAId = Shader.PropertyToID("_MeadowWindA");
        static readonly int WindBId = Shader.PropertyToID("_MeadowWindB");
        static readonly int HandsId = Shader.PropertyToID("_MeadowHands");
        static readonly int HandVelId = Shader.PropertyToID("_MeadowHandVel");
        static readonly int HandGustId = Shader.PropertyToID("_MeadowHandGust");
        static readonly int MuteId = Shader.PropertyToID("_MeadowWindMute");
        static readonly int Mute2Id = Shader.PropertyToID("_MeadowWindMute2");

        static int _liveInstances;

        /// <summary>How many wind buffers exist right now (1 in a running meadow; a test of leaks reads it).</summary>
        public static int LiveInstances => _liveInstances;

        readonly GraphicsBuffer _buffer;
        bool _disposed;
        readonly WindWaveGpu[] _scratch = new WindWaveGpu[Capacity];
        readonly Vector4[] _hands = new Vector4[HandSlots];
        readonly Vector4[] _handVelocity = new Vector4[HandSlots];
        readonly MeadowWindDto _settings;

        public WindFieldGpu(MeadowWindDto settings, WindTerm muted = WindTerm.None)
        {
            _settings = settings;
            var keep = WindMute.ToShaderFlags(muted);   // 1 = the term works, 0 = muted (--wind-mute)
            Shader.SetGlobalVector(MuteId, new Vector4(keep.breeze, keep.waves, keep.hands, keep.sparkle));
            Shader.SetGlobalVector(Mute2Id, new Vector4(keep.gusts, keep.sway, keep.flutter, 0f));
            _liveInstances++;
            _buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Capacity, WindWaveGpu.SizeBytes);
            _buffer.SetData(_scratch);
            Shader.SetGlobalBuffer(WavesId, _buffer);
            Shader.SetGlobalVector(WindAId, new Vector4(settings.waveWidth, settings.bendScale, BladeBend.MaxBendRadians(settings.maxBendDeg), 0f));   // z: the most a blade bends in total (MeadowBladeCurve)
            Push(0);
        }

        /// <summary>How many waves were uploaded by the last <see cref="Upload"/>.</summary>
        public int LiveWaves { get; private set; }

        /// <summary>What a hand slot blows with now: whether it is active, its place on the ground (metres) and its velocity (metres per second). For tests and the debug overlay.</summary>
        public (bool active, float x, float z, float vx, float vz) HandAt(int slot) =>
            (_hands[slot].z > 0.5f, _hands[slot].x, _hands[slot].y, _handVelocity[slot].x, _handVelocity[slot].y);

        /// <summary>Where a hand is on the ground (metres) and how fast it moves (metres per second); inactive hands push nothing.</summary>
        public void SetHand(int slot, bool active, float x, float z, float velocityX, float velocityZ)
        {
            _hands[slot] = new Vector4(x, z, active ? 1f : 0f, 0f);
            _handVelocity[slot] = new Vector4(velocityX, velocityZ, 0f, 0f);
        }

        public void Upload(WindField field, double now)
        {
            LiveWaves = field.CopyActive(now, _scratch);
            if (LiveWaves > 0) _buffer.SetData(_scratch, 0, 0, LiveWaves);
            Push(LiveWaves);
        }

        void Push(int waveCount)
        {
            Shader.SetGlobalVector(WindBId, new Vector4(waveCount, _settings.handRadius, _settings.handPush, _settings.handLean));
            Shader.SetGlobalVectorArray(HandsId, _hands);
            Shader.SetGlobalVectorArray(HandVelId, _handVelocity);
            Shader.SetGlobalVector(HandGustId, new Vector4(_settings.handGustWidth, _settings.handSpeedCap, 0f, 0f));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _liveInstances--;
            _buffer.Release();
        }
    }
}
